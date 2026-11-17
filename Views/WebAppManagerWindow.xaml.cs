using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class WebAppManagerWindow : Window
{
    private readonly string _id;
    private readonly Dictionary<string, CheckBox> _typeBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _extensionBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, CheckBox>> _permissionBoxes = new(StringComparer.OrdinalIgnoreCase);

    private WebAppAccessPolicy _policy = new();
    private List<WebAppExtensionInfo> _extensions = new();
    private bool _building;

    public WebAppManagerWindow(string webAppId)
    {
        _id = webAppId;
        InitializeComponent();

        var manifest = WebAppService.Load(_id);
        string name = manifest?.Name ?? _id;
        Title = "Web App Settings - " + name;
        TxtTitle.Text = name;
        TxtUrl.Text = manifest?.StartUrl ?? "";
        TxtName.Text = name;
        ChkOffer.IsChecked = SettingsService.Current.WebAppOfferOnLeave;

        _policy = WebAppAccessService.Load(_id);
        _extensions = WebAppAccessService.ListInstalledExtensions();
        BuildUi();
    }

    private static CheckBox MakeCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            IsChecked = isChecked,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private static TextBlock MakeNote(string text)
    {
        return new TextBlock { Text = text, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    }

    private void BuildUi()
    {
        _building = true;
        try
        {
            ChkExtensionsEnabled.IsChecked = _policy.ExtensionsEnabled;
            PnlTypes.Children.Clear();
            PnlExtensions.Children.Clear();
            _typeBoxes.Clear();
            _extensionBoxes.Clear();
            _permissionBoxes.Clear();

            var allTypes = _extensions
                .SelectMany(e => e.Permissions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p == WebAppAccessService.SiteAccess ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string permission in allTypes)
            {
                bool allowed = !_policy.DeniedPermissionTypes.Contains(permission, StringComparer.OrdinalIgnoreCase);
                var box = MakeCheckBox(WebAppAccessService.Describe(permission), allowed);
                box.Checked += AnyBox_Changed;
                box.Unchecked += AnyBox_Changed;
                _typeBoxes[permission] = box;
                PnlTypes.Children.Add(box);
            }
            if (allTypes.Count == 0)
                PnlTypes.Children.Add(MakeNote("No installed extension requests any permissions."));

            foreach (var extension in _extensions)
            {
                _policy.Extensions.TryGetValue(extension.FolderName, out var access);
                access ??= new WebAppExtensionAccess();

                var allowBox = MakeCheckBox(extension.Name, access.Allowed);
                allowBox.FontWeight = FontWeights.SemiBold;
                allowBox.Checked += AnyBox_Changed;
                allowBox.Unchecked += AnyBox_Changed;
                _extensionBoxes[extension.FolderName] = allowBox;

                var permissionPanel = new StackPanel { Margin = new Thickness(26, 4, 0, 8) };
                var boxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
                foreach (string permission in extension.Permissions)
                {
                    bool granted = !access.DeniedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
                    var box = MakeCheckBox(WebAppAccessService.Describe(permission), granted);
                    box.Checked += AnyBox_Changed;
                    box.Unchecked += AnyBox_Changed;
                    boxes[permission] = box;
                    permissionPanel.Children.Add(box);
                }
                if (extension.Permissions.Count == 0)
                    permissionPanel.Children.Add(MakeNote("This extension requests no special permissions."));
                _permissionBoxes[extension.FolderName] = boxes;

                PnlExtensions.Children.Add(new Expander
                {
                    Header = allowBox,
                    Content = permissionPanel,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
            if (_extensions.Count == 0)
                PnlExtensions.Children.Add(MakeNote("No extensions are installed."));
        }
        finally
        {
            _building = false;
        }

        SyncAvailability();
        UpdateIsolationNote();
    }

    private WebAppAccessPolicy CollectPolicy()
    {
        var policy = new WebAppAccessPolicy { ExtensionsEnabled = ChkExtensionsEnabled.IsChecked == true };

        foreach (var pair in _typeBoxes)
        {
            if (pair.Value.IsChecked != true) policy.DeniedPermissionTypes.Add(pair.Key);
        }
        foreach (string permission in _policy.DeniedPermissionTypes)
        {
            if (!_typeBoxes.ContainsKey(permission) && !policy.DeniedPermissionTypes.Contains(permission, StringComparer.OrdinalIgnoreCase))
                policy.DeniedPermissionTypes.Add(permission);
        }

        foreach (var extension in _extensions)
        {
            var access = new WebAppExtensionAccess { Allowed = _extensionBoxes[extension.FolderName].IsChecked == true };
            foreach (var pair in _permissionBoxes[extension.FolderName])
            {
                if (pair.Value.IsChecked != true) access.DeniedPermissions.Add(pair.Key);
            }
            if (!access.Allowed || access.DeniedPermissions.Count > 0)
                policy.Extensions[extension.FolderName] = access;
        }
        foreach (var pair in _policy.Extensions)
        {
            if (!_extensionBoxes.ContainsKey(pair.Key)) policy.Extensions[pair.Key] = pair.Value;
        }

        return policy;
    }

    private void SyncAvailability()
    {
        bool master = ChkExtensionsEnabled.IsChecked == true;
        PnlTypes.IsEnabled = master;
        PnlExtensions.IsEnabled = master;

        foreach (var extension in _extensions)
        {
            bool allowed = _extensionBoxes[extension.FolderName].IsChecked == true;
            foreach (var pair in _permissionBoxes[extension.FolderName])
            {
                bool typeBlocked = _typeBoxes.TryGetValue(pair.Key, out var typeBox) && typeBox.IsChecked != true;
                pair.Value.IsEnabled = allowed && !typeBlocked;
                pair.Value.ToolTip = typeBlocked ? "Blocked for every extension in this Web App" : null;
            }
        }
    }

    private void UpdateIsolationNote()
    {
        bool restricted = WebAppAccessService.RequiresIsolation(CollectPolicy());
        TxtIsolationNote.Text = restricted
            ? "Restricted: this Web App runs in its own isolated profile with only the extensions and permissions allowed below. Its sign-in is kept separate from the browser, so you sign in again the first time."
            : "No restrictions: this Web App shares the browser profile and every extension has full access.";
    }

    private bool SavePolicy()
    {
        var policy = CollectPolicy();
        if (!WebAppAccessService.Save(_id, policy))
        {
            TxtStatus.Text = "Could not save the access settings.";
            return false;
        }
        _policy = policy;
        TxtStatus.Text = "Saved. Changes apply the next time this Web App starts.";
        return true;
    }

    private void AnyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_building || PnlTypes == null) return;
        SyncAvailability();
        UpdateIsolationNote();
        TxtStatus.Text = "Unsaved changes.";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SavePolicy();

    private async void BtnSaveRestart_Click(object sender, RoutedEventArgs e)
    {
        if (!SavePolicy()) return;
        bool restarted = await WebAppHostService.RestartByIdAsync(_id);
        TxtStatus.Text = restarted
            ? "Saved and restarted."
            : "Saved. This Web App is not running here, so changes apply the next time it starts.";
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _policy = new WebAppAccessPolicy();
        BuildUi();
        TxtStatus.Text = "Reset to full access. Press Save to apply.";
    }

    private void ChkOffer_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.WebAppOfferOnLeave = ChkOffer.IsChecked == true;
        SettingsService.Save();
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        bool ok = WebAppService.Rename(_id, TxtName.Text, errors);
        if (ok)
        {
            var manifest = WebAppService.Load(_id);
            string name = manifest?.Name ?? TxtName.Text;
            TxtTitle.Text = name;
            Title = "Web App Settings - " + name;
        }
        TxtGeneralStatus.Text = ok
            ? "Renamed. Shortcuts updated." + (errors.Count > 0 ? " Warnings: " + string.Join("; ", errors) : "")
            : "Rename failed: " + string.Join("; ", errors);
    }

    private void BtnShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        bool ok = WebAppService.RecreateShortcuts(
            _id,
            SettingsService.Current.WebAppShortcutDesktop,
            SettingsService.Current.WebAppShortcutStartMenu,
            errors);
        TxtGeneralStatus.Text = ok ? "Shortcuts recreated." : "Shortcuts: " + string.Join("; ", errors);
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        string name = WebAppService.Load(_id)?.Name ?? _id;
        var answer = MessageBox.Show(
            this,
            "Uninstall '" + name + "'?\n\nIts shortcuts and saved files will be deleted.",
            "Horizon",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var errors = new List<string>();
        bool ok = WebAppService.Uninstall(_id, errors);
        if (ok)
        {
            if (IsLoaded) Close();
            return;
        }
        TxtGeneralStatus.Text = "Uninstall incomplete: " + string.Join("; ", errors);
    }
}
