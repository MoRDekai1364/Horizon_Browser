using System;
using System.Windows;
using System.Windows.Input;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public enum UpdateUserDecision
{
    UpdateNow,
    UpdateLaterDownloadNow,
    UpdateLaterDownloadLater,
    SnoozeWeek,
    SnoozeMonth,
    SnoozeYear,
    SkipVersion,
    Dismiss
}

public partial class UpdatePromptDialog : Window
{
    private readonly UpdateCheckResult _result;
    private readonly string _targetInstallDir;

    public UpdateUserDecision Decision { get; private set; } = UpdateUserDecision.Dismiss;

    public UpdatePromptDialog(UpdateCheckResult result, string targetInstallDir)
    {
        InitializeComponent();
        _result = result;
        _targetInstallDir = targetInstallDir;

        TxtVersionSummary.Text = $"Version {result.LatestVersion} is available for the {result.CurrentChannel} channel.";
        TxtCurrentVersionBadge.Text = $"Current: v{result.CurrentVersion} ({result.CurrentChannel})";
        TxtTargetLocationBadge.Text = $"Target: {_targetInstallDir}";
        TxtTargetLocationBadge.ToolTip = _targetInstallDir;
        TxtNowTargetDir.Text = $"Location: {_targetInstallDir}";
        TxtNeverOption.Text = $"Never for version v{result.LatestVersion} (resets when next version is released)";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Decision = UpdateUserDecision.Dismiss;
        DialogResult = false;
        Close();
    }

    private void CardYes_Click(object sender, MouseButtonEventArgs e)
    {
        PanelStateQuestion.Visibility = Visibility.Collapsed;
        PanelStateNoSnooze.Visibility = Visibility.Collapsed;
        PanelStateYesOptions.Visibility = Visibility.Visible;
        BtnBack.Visibility = Visibility.Visible;
    }

    private void CardNo_Click(object sender, MouseButtonEventArgs e)
    {
        PanelStateQuestion.Visibility = Visibility.Collapsed;
        PanelStateYesOptions.Visibility = Visibility.Collapsed;
        PanelStateNoSnooze.Visibility = Visibility.Visible;
        BtnBack.Visibility = Visibility.Visible;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        PanelStateYesOptions.Visibility = Visibility.Collapsed;
        PanelStateNoSnooze.Visibility = Visibility.Collapsed;
        PanelStateQuestion.Visibility = Visibility.Visible;
        BtnBack.Visibility = Visibility.Collapsed;
    }

    private void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        Decision = UpdateUserDecision.UpdateNow;
        DialogResult = true;
        Close();
    }

    private void BtnConfirmUpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (RbDownloadNow.IsChecked == true)
        {
            Decision = UpdateUserDecision.UpdateLaterDownloadNow;
        }
        else
        {
            Decision = UpdateUserDecision.UpdateLaterDownloadLater;
        }

        SettingsService.Current.PendingUpdateDownloadChoice = Decision == UpdateUserDecision.UpdateLaterDownloadNow ? "DownloadNow" : "DownloadLater";
        SettingsService.Save();

        DialogResult = true;
        Close();
    }

    private void BtnSaveSnooze_Click(object sender, RoutedEventArgs e)
    {
        if (RbSnoozeWeek.IsChecked == true)
        {
            Decision = UpdateUserDecision.SnoozeWeek;
            GithubUpdateService.SnoozeUpdates("week", _result.LatestVersion);
        }
        else if (RbSnoozeMonth.IsChecked == true)
        {
            Decision = UpdateUserDecision.SnoozeMonth;
            GithubUpdateService.SnoozeUpdates("month", _result.LatestVersion);
        }
        else if (RbSnoozeYear.IsChecked == true)
        {
            Decision = UpdateUserDecision.SnoozeYear;
            GithubUpdateService.SnoozeUpdates("year", _result.LatestVersion);
        }
        else if (RbSnoozeNever.IsChecked == true)
        {
            Decision = UpdateUserDecision.SkipVersion;
            GithubUpdateService.SnoozeUpdates("never", _result.LatestVersion);
        }

        DialogResult = false;
        Close();
    }
}
