using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using Horizon.Stealth.Services;
using System.Linq;
using System;
using Microsoft.Win32;

namespace Horizon.Stealth.Controls;

public partial class VaultSidebar : UserControl
{
    private ICollectionView? _vaultView;
    private VaultItem? _itemBeingEdited;

    public VaultSidebar()
    {
        InitializeComponent();
        RefreshList();
        VaultService.OnUpdated += RefreshList;
    }

    private void RefreshList()
    {
        Dispatcher.Invoke(() => 
        {
            ListVault.ItemsSource = VaultService.Items;
            _vaultView = CollectionViewSource.GetDefaultView(ListVault.ItemsSource);
            if (_vaultView != null)
            {
                _vaultView.Filter = item => IsMatch((VaultItem)item, TxtSearch.Text.ToLower());
            }

            OverlayLocked.Visibility = VaultService.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vaultView?.Refresh();
    }

    private bool IsMatch(VaultItem item, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return item.Url.ToLower().Contains(query) || 
               item.Username.ToLower().Contains(query);
    }

    private void BtnAddNew_Click(object sender, RoutedEventArgs e)
    {
        _itemBeingEdited = null;
        TxtOverlayHeader.Text = "ADD CREDENTIAL";
        TxtEditTitle.Text = "";
        TxtEditUrl.Text   = "";
        TxtEditUser.Text  = "";
        TxtEditPass.Text  = "";
        OverlayEdit.Visibility = Visibility.Visible;
    }

    private void BtnCopyUser_Click(object sender, RoutedEventArgs e)
    {
        if (ListVault.SelectedItem is VaultItem item)
        {
            try { Clipboard.SetText(item.Username); } catch { }
        }
    }

    private void BtnCopyPass_Click(object sender, RoutedEventArgs e)
    {
        if (ListVault.SelectedItem is VaultItem item)
        {
            try { Clipboard.SetText(item.Password); } catch { }
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ListVault.SelectedItem is VaultItem item)
        {
            _itemBeingEdited = item;
            TxtOverlayHeader.Text = "EDIT CREDENTIAL";
            TxtEditTitle.Text = item.Title;
            TxtEditUrl.Text = item.Url;
            TxtEditUser.Text = item.Username;
            TxtEditPass.Text = item.Password;
            OverlayEdit.Visibility = Visibility.Visible;
        }
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        OverlayEdit.Visibility = Visibility.Collapsed;
        _itemBeingEdited = null;
    }

    private void BtnSaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_itemBeingEdited != null)
        {
            VaultService.UpdateItem(_itemBeingEdited, TxtEditTitle.Text, TxtEditUrl.Text, TxtEditUser.Text, TxtEditPass.Text);
        }
        else if (!string.IsNullOrEmpty(TxtEditPass.Text))
        {
            VaultService.Add(TxtEditUrl.Text, TxtEditUser.Text, TxtEditPass.Text, TxtEditTitle.Text);
        }
        OverlayEdit.Visibility = Visibility.Collapsed;
        _itemBeingEdited = null;
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ListVault.SelectedItem is VaultItem item)
        {
            VaultService.Remove(item);
        }
    }

    private void BtnLockVault_Click(object sender, RoutedEventArgs e)
    {
        VaultService.Lock();
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        VaultService.Unlock();
    }

    private void BtnWipeImport_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("This will delete ALL saved credentials and re-import from CSV.\n\nContinue?", "Horizon Vault", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var dialog = new OpenFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            Title = "Wipe & Re-import Credentials"
        };

        if (dialog.ShowDialog() == true)
        {
            VaultService.WipeAndImport(dialog.FileName);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                Title = "Import Credentials"
            };

            if (dialog.ShowDialog() == true)
            {
                VaultService.ImportCsv(dialog.FileName);
            }
        }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            FileName = "horizon_vault_export.csv",
            Title = "Export Credentials (UNENCRYPTED)"
        };

        if (dialog.ShowDialog() == true)
        {
            VaultService.ExportCsv(dialog.FileName);
        }
    }

    private void ListVault_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ListVault.SelectedItem is VaultItem item)
        {
            TriggerAutofill(item);
        }
    }

    private async void TriggerAutofill(VaultItem item)
    {
        try
        {
            if (Window.GetWindow(this) is MainWindow mainWin && mainWin.CurrentBrowser != null)
            {
                string safeUser = item.Username.Replace("\\", "\\\\").Replace("'", "\\'");
                string safePass = item.Password.Replace("\\", "\\\\").Replace("'", "\\'");

                string script = $@"
                    (function() {{
                        var user = '{safeUser}';
                        var pass = '{safePass}';
                        var inputs = Array.from(document.querySelectorAll('input'));
                        var passInput = inputs.find(i => i.type === 'password');
                        var userInput = null;

                        if (passInput) {{
                            var idx = inputs.indexOf(passInput);
                            for (var i = idx - 1; i >= 0; i--) {{
                                var t = inputs[i].type;
                                var id = (inputs[i].id || '').toLowerCase();
                                if (t === 'text' || t === 'email' || id.includes('user') || id.includes('login')) {{
                                    userInput = inputs[i];
                                    break;
                                }}
                            }}
                        }}

                        var filled = 0;
                        if (userInput && user) {{ 
                            userInput.value = user; 
                            userInput.dispatchEvent(new Event('input', {{ bubbles: true }}));
                            userInput.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            filled++;
                        }}
                        
                        if (passInput && pass) {{ 
                            passInput.value = pass; 
                            passInput.dispatchEvent(new Event('input', {{ bubbles: true }})); 
                            passInput.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            filled++;
                        }}
                        
                        return filled;
                    }})();";

                string result = await mainWin.CurrentBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(script);
                
                if (result == "0" || result == "null")
                {
                    try { Clipboard.SetText(item.Password); } catch { }
                    MessageBox.Show("Password copied to clipboard. No fields detected.", "Horizon Vault", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Vault Autofill");
        }
    }
}