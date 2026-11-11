using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Core;
using Horizon.Stealth.Services;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth.Views;

public partial class ExtensionInstallDialog : Window
{
    private readonly StorePageInfo _storeInfo;
    private readonly CoreWebView2Profile? _profile;
    private readonly Action<ExtensionRecord>? _onSuccess;
    private bool _isInstalling;
    private string? _stagedFolder;
    private List<ExtensionPermissionModel> _permissionsList = new();

    public ExtensionInstallDialog(StorePageInfo storeInfo, CoreWebView2Profile? profile = null, Action<ExtensionRecord>? onSuccess = null, Window? owner = null)
    {
        InitializeComponent();
        _storeInfo = storeInfo;
        _profile = profile;
        _onSuccess = onSuccess;
        if (owner != null) Owner = owner;

        TxtTitle.Text = string.IsNullOrWhiteSpace(storeInfo.Name) ? storeInfo.ExtensionId : storeInfo.Name;
        TxtSubtitle.Text = storeInfo.Kind switch
        {
            StorePageInfo.StoreKind.Chrome => "Chrome Web Store",
            StorePageInfo.StoreKind.Edge => "Microsoft Edge Add-ons",
            StorePageInfo.StoreKind.Firefox => "Firefox Add-ons",
            _ => "Web Extension"
        };

        Closing += (_, _) =>
        {
            if (!_isInstalling && !string.IsNullOrEmpty(_stagedFolder) && Directory.Exists(_stagedFolder))
            {
                try { Directory.Delete(_stagedFolder, recursive: true); } catch { }
            }
        };

        Loaded += async (_, _) => await LoadPermissionsPreviewAsync();
    }

    private async Task LoadPermissionsPreviewAsync()
    {
        TxtStatus.Text = "Inspecting extension permissions…";
        ProgressBarInstall.Visibility = Visibility.Visible;

        try
        {
            string? stage = await Task.Run(async () =>
            {
                // Check if already installed locally first
                string localFolder = Path.Combine(ConfigService.ExtensionsRoot, _storeInfo.ExtensionId);
                string manifestPath = Path.Combine(localFolder, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    return localFolder;
                }
                return await ExtensionInstaller.DownloadAndStageAsync(_storeInfo);
            });

            _stagedFolder = stage;

            if (!string.IsNullOrEmpty(_stagedFolder))
            {
                string manifestPath = Path.Combine(_stagedFolder, "manifest.json");
                _permissionsList = ExtensionInstaller.ParseManifestPermissionsDetailed(manifestPath);
            }

            if (_permissionsList.Count == 0)
            {
                _permissionsList = new List<ExtensionPermissionModel>
                {
                    new ExtensionPermissionModel
                    {
                        Key = "standard",
                        DisplayName = "Standard Extension Capabilities",
                        Description = "Local storage, active tab interaction, and content script styling",
                        IsEssential = true,
                        IsSelected = true
                    }
                };
            }

            IcnPermissions.ItemsSource = _permissionsList;
            TxtStatus.Text = "Review requested permissions and choose what to permit.";
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "LoadPermissionsPreviewAsync");
            _permissionsList = new List<ExtensionPermissionModel>
            {
                new ExtensionPermissionModel
                {
                    Key = "standard",
                    DisplayName = "Standard Permissions",
                    Description = "Basic tab and storage access",
                    IsEssential = true,
                    IsSelected = true
                }
            };
            IcnPermissions.ItemsSource = _permissionsList;
            TxtStatus.Text = "Ready to install.";
        }
        finally
        {
            ProgressBarInstall.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        _isInstalling = true;
        BtnInstall.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        ProgressBarInstall.Visibility = Visibility.Visible;

        var progress = new Progress<string>(msg =>
        {
            Dispatcher.Invoke(() => TxtStatus.Text = msg);
        });

        try
        {
            var disabledKeys = _permissionsList
                .Where(p => !p.IsSelected && !p.IsEssential)
                .Select(p => p.Key)
                .ToList();

            ExtensionRecord? record;
            if (!string.IsNullOrEmpty(_stagedFolder) && Directory.Exists(_stagedFolder) && !_stagedFolder.Equals(Path.Combine(ConfigService.ExtensionsRoot, _storeInfo.ExtensionId), StringComparison.OrdinalIgnoreCase))
            {
                record = await ExtensionInstaller.FinalizeInstallFromStageAsync(_stagedFolder, _storeInfo, disabledKeys, progress);
            }
            else
            {
                record = await ExtensionInstaller.InstallAsync(_storeInfo, progress);
                if (record != null && disabledKeys.Count > 0)
                {
                    string finalManifest = Path.Combine(ExtensionService.GetInstallFolder(record.FolderName), "manifest.json");
                    ExtensionInstaller.ApplyPermissionChoices(finalManifest, disabledKeys);
                }
            }

            if (record != null)
            {
                string installFolder = ExtensionService.GetInstallFolder(record.FolderName);
                if (_profile != null && Directory.Exists(installFolder))
                {
                    try
                    {
                        var ext = await _profile.AddBrowserExtensionAsync(installFolder);
                        if (ext != null && !ext.IsEnabled)
                        {
                            await ext.EnableAsync(true);
                        }
                        Controls.BrowserView.RegisterLoadedExtension(record.FolderName, ext?.Id ?? record.Id, record.Name);
                        LogService.Write("EXT", $"Dynamically loaded extension '{record.Name}' into session!");
                    }
                    catch (Exception ex)
                    {
                        LogService.RecordCrash(ex, "DynamicExtensionLoad");
                    }
                }

                TxtStatus.Text = "✓ Extension successfully added!";
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                _onSuccess?.Invoke(record);

                await Task.Delay(600);
                DialogResult = true;
                Close();
            }
            else
            {
                TxtStatus.Text = "Installation failed. Check network or logs.";
                BtnInstall.IsEnabled = true;
                BtnCancel.IsEnabled = true;
                ProgressBarInstall.Visibility = Visibility.Collapsed;
                _isInstalling = false;
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
            BtnInstall.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            ProgressBarInstall.Visibility = Visibility.Collapsed;
            _isInstalling = false;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_stagedFolder) && Directory.Exists(_stagedFolder) &&
            !_stagedFolder.Equals(Path.Combine(ConfigService.ExtensionsRoot, _storeInfo.ExtensionId), StringComparison.OrdinalIgnoreCase))
        {
            try { Directory.Delete(_stagedFolder, recursive: true); } catch { }
        }
        DialogResult = false;
        Close();
    }
}
