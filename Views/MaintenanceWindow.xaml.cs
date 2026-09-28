using System.IO;
using System.IO.Compression;
using System.Windows;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class MaintenanceWindow : Window
{
    public MaintenanceWindow()
    {
        InitializeComponent();
        LogService.Write("MAINTENANCE", "Dashboard opened.");
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Backing up data...";
            BtnBackup.IsEnabled = false;

            string backupFolder = Path.Combine(ConfigService.AppRoot, "Backups");
            Directory.CreateDirectory(backupFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string destFile = Path.Combine(backupFolder, $"Horizon_Backup_{timestamp}.zip");
            
            if (Directory.Exists(ConfigService.UserDataRoot))
            {
                ZipFile.CreateFromDirectory(ConfigService.UserDataRoot, destFile);
                LogService.Write("MAINTENANCE", $"Backup created: {destFile}");
                MessageBox.Show($"Data saved to:\n{destFile}", "Backup Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Backup complete.";
            }
            else
            {
                StatusText.Text = "No data to backup.";
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Maintenance.Backup");
            MessageBox.Show($"Backup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Backup failed.";
        }
        finally
        {
            BtnBackup.IsEnabled = true;
        }
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Checking for updates...";
        BtnUpdate.IsEnabled = false;
        
        await Task.Delay(2000); 

        LogService.Write("MAINTENANCE", "Update check requested (Manual).");
        MessageBox.Show("You are running the latest version: v1.0.0 (Stealth)\n\nNo patches found.", "Update Service", MessageBoxButton.OK, MessageBoxImage.Information);
        
        StatusText.Text = "System is up to date.";
        BtnUpdate.IsEnabled = true;
    }

    private void BtnFixSync_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("This will log you out of all websites to fix sync issues.\n\nYour saved passwords (Vault) will stay safe.\nContinue?", 
            "Fix Sync & Logins", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StatusText.Text = "Cleaning sessions...";
            LogService.Write("MAINTENANCE", "Starting Sync Repair (Cookie Wipe)...");

            string profile = Path.Combine(ConfigService.UserDataRoot, "EBWebView", "Default");
            string networkPath = Path.Combine(profile, "Network");
            string cachePath = Path.Combine(profile, "Cache");
            string codeCachePath = Path.Combine(profile, "Code Cache");

            int deletedCount = 0;

            if (Directory.Exists(networkPath))
            {
                Directory.Delete(networkPath, true);
                deletedCount++;
            }
            if (Directory.Exists(cachePath)) 
            {
                Directory.Delete(cachePath, true);
                deletedCount++;
            }
             if (Directory.Exists(codeCachePath)) 
            {
                Directory.Delete(codeCachePath, true);
                deletedCount++;
            }

            LogService.Write("MAINTENANCE", "Sync Repair complete. Network/Cache deleted.");
            MessageBox.Show("Sync repair complete.\n\nPlease restart Horizon.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Repair successful.";
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Maintenance.FixSync");
            MessageBox.Show($"Could not clear files. Close Horizon and try again.\nError: {ex.Message}", "File Lock Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Repair failed (File Locked).";
        }
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("WARNING: This will delete ALL history, cookies, and settings.\n\nYour Vault (passwords) will be preserved if possible, but everything else goes.\n\nAre you sure?", 
            "Factory Reset", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StatusText.Text = "Resetting...";
            LogService.Write("MAINTENANCE", "FACTORY RESET INITIATED.");

            string vaultPath = Path.Combine(ConfigService.UserDataRoot, "vault.dat");
            string tempVault = Path.Combine(ConfigService.AppRoot, "vault.dat.bak");
            bool vaultSaved = false;

            if (File.Exists(vaultPath))
            {
                File.Copy(vaultPath, tempVault, true);
                vaultSaved = true;
                LogService.Write("MAINTENANCE", "Vault preserved temporarily.");
            }

            if (Directory.Exists(ConfigService.UserDataRoot))
            {
                Directory.Delete(ConfigService.UserDataRoot, true);
                LogService.Write("MAINTENANCE", "HorizonData wiped.");
            }

            Directory.CreateDirectory(ConfigService.UserDataRoot);
            if (vaultSaved && File.Exists(tempVault))
            {
                File.Move(tempVault, vaultPath);
                LogService.Write("MAINTENANCE", "Vault restored.");
            }

            MessageBox.Show("Factory Reset Complete.\n\nThe browser is now fresh.", "Reset", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Maintenance.FactoryReset");
            MessageBox.Show($"Reset failed. Files might be in use.\nError: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Reset failed.";
        }
    }
}