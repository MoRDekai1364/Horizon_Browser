using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class BackupRestoreDialog : Window
{
    public BackupRestoreDialog()
    {
        InitializeComponent();
        RefreshBackupsList();
    }

    private void RefreshBackupsList()
    {
        var backups = BackupService.GetAvailableBackups();
        ListBackups.ItemsSource = backups;
        BtnRestoreSelected.IsEnabled = false;
        if (backups.Count > 0)
        {
            ListBackups.SelectedIndex = 0;
        }
    }

    private void ListBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnRestoreSelected.IsEnabled = ListBackups.SelectedItem is BackupInfo;
    }

    private async void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateBackup.IsEnabled = false;
        PanelProgress.Visibility = Visibility.Visible;
        ProgressBarStatus.Value = 0;

        var progress = new Progress<(string step, int percent)>(report =>
        {
            ProgressBarStatus.Value = report.percent;
            TxtStatus.Text = $"{report.step} ({report.percent}%)";
        });

        try
        {
            string createdPath = await BackupService.CreateBackupAsync(null, progress);
            MessageBox.Show(
                $"Backup successfully created!\n\nLocation: {createdPath}",
                "Backup Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RefreshBackupsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to create backup:\n{ex.Message}",
                "Backup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnCreateBackup.IsEnabled = true;
            PanelProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnRestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ListBackups.SelectedItem is not BackupInfo info) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to restore backup '{info.Name}'?\n\nThis will restore your settings, vault, and profile data.",
            "Confirm Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        await PerformRestoreAsync(info.Path);
    }

    private async void BtnBrowseOther_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Horizon Backup Folder",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
        {
            var confirm = MessageBox.Show(
                $"Restore Horizon data from:\n{dialog.SelectedPath}?\n\nExisting settings and data will be restored.",
                "Confirm Custom Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await PerformRestoreAsync(dialog.SelectedPath);
            }
        }
    }

    private async System.Threading.Tasks.Task PerformRestoreAsync(string folderPath)
    {
        PanelProgress.Visibility = Visibility.Visible;
        ProgressBarStatus.Value = 0;
        BtnCreateBackup.IsEnabled = false;
        BtnRestoreSelected.IsEnabled = false;

        var progress = new Progress<(string step, int percent)>(report =>
        {
            ProgressBarStatus.Value = report.percent;
            TxtStatus.Text = $"{report.step} ({report.percent}%)";
        });

        try
        {
            await BackupService.RestoreBackupAsync(folderPath, progress);
            MessageBox.Show(
                "Backup successfully restored!\n\nPlease restart Horizon Browser for all profile changes to take effect.",
                "Restore Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to restore backup:\n{ex.Message}",
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PanelProgress.Visibility = Visibility.Collapsed;
            BtnCreateBackup.IsEnabled = true;
            RefreshBackupsList();
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(BackupService.DefaultBackupsRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = BackupService.DefaultBackupsRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open backups folder: {ex.Message}", "Error");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
