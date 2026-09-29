using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class VpnControlCenterWindow : Window
{
    private string _vpnEditingId = "";
    private string _vpnActiveId = "";
    private bool _loading;

    private DispatcherTimer? _liveTimer;

    public VpnControlCenterWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadVpnSection();
        BuildAdGuardSection();
        RefreshVpnList(_vpnActiveId);
        DrawThroughputHistory();
        UpdateStatusHeader();

        VpnRelayService.StateChanged += OnVpnStateChanged;

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _liveTimer.Tick += (s, ev) => { UpdateStatusHeader(); DrawThroughputHistory(); };
        _liveTimer.Start();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        VpnRelayService.StateChanged -= OnVpnStateChanged;
        _liveTimer?.Stop();
    }

    private void OnVpnStateChanged(VpnRelayState state, string message) =>
        Dispatcher.BeginInvoke(UpdateStatusHeader);

    private void UpdateStatusHeader()
    {
        var state = VpnRelayService.State;
        var profile = VpnRelayService.ActiveProfile;
        var suffix = profile != null ? $" via {profile.Name}" + (string.IsNullOrEmpty(profile.Country) ? "" : $" ({profile.Country})") : "";
        TblStatusHeader.Text = $"VPN status: {state}{suffix}";
    }

    // ── VPN section ─────────────────────────────────────────────────────────

    private void LoadVpnSection()
    {
        _loading = true;
        var s = SettingsService.Current;

        ChkVpnEnabled.IsChecked = s.VpnEnabled;
        ChkVpnAutoConnect.IsChecked = s.VpnAutoConnect;
        ChkVpnKillSwitch.IsChecked = s.VpnKillSwitchEnabled;
        ChkVpnNotify.IsChecked = s.VpnNotifyOnFallback;
        ChkVpnBlockWebRtc.IsChecked = s.VpnBlockWebRtcLeak;
        TxtVpnBypass.Text = s.VpnBypassList ?? "";
        _vpnActiveId = s.VpnActiveProfileId ?? "";

        foreach (ComboBoxItem item in CboGeoLookupMode.Items)
        {
            if ((string)item.Tag == s.GeoLookupMode) { CboGeoLookupMode.SelectedItem = item; break; }
        }
        if (CboGeoLookupMode.SelectedItem == null) CboGeoLookupMode.SelectedIndex = 0;

        ChkVpnEnabled.Checked   += (_, _) => SaveVpnSettings();
        ChkVpnEnabled.Unchecked += (_, _) => SaveVpnSettings();
        ChkVpnAutoConnect.Checked   += (_, _) => SaveVpnSettings();
        ChkVpnAutoConnect.Unchecked += (_, _) => SaveVpnSettings();
        ChkVpnKillSwitch.Checked   += (_, _) => SaveVpnSettings();
        ChkVpnKillSwitch.Unchecked += (_, _) => SaveVpnSettings();
        ChkVpnNotify.Checked   += (_, _) => SaveVpnSettings();
        ChkVpnNotify.Unchecked += (_, _) => SaveVpnSettings();
        ChkVpnBlockWebRtc.Checked   += (_, _) => SaveVpnSettings();
        ChkVpnBlockWebRtc.Unchecked += (_, _) => SaveVpnSettings();
        TxtVpnBypass.LostFocus += (_, _) => SaveVpnSettings();
        CboGeoLookupMode.SelectionChanged += (_, _) => SaveVpnSettings();

        ClearVpnEditor();
        _loading = false;
    }

    private void SaveVpnSettings()
    {
        if (_loading) return;
        var s = SettingsService.Current;
        s.VpnEnabled = ChkVpnEnabled.IsChecked == true;
        s.VpnAutoConnect = ChkVpnAutoConnect.IsChecked == true;
        s.VpnKillSwitchEnabled = ChkVpnKillSwitch.IsChecked == true;
        s.VpnNotifyOnFallback = ChkVpnNotify.IsChecked == true;
        s.VpnBlockWebRtcLeak = ChkVpnBlockWebRtc.IsChecked == true;
        s.VpnBypassList = TxtVpnBypass.Text.Trim();
        s.VpnActiveProfileId = _vpnActiveId;
        s.GeoLookupMode = (CboGeoLookupMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "Local";
        SettingsService.Save();
    }

    private void RefreshVpnList(string? selectId)
    {
        LstVpnProfiles.Items.Clear();
        ListBoxItem? toSelect = null;
        foreach (var p in VpnProfileStore.Profiles)
        {
            var label = $"{p.Name}  ({p.Type}, {p.Host}:{p.Port}{(string.IsNullOrEmpty(p.Country) ? "" : ", " + p.Country)})" + (p.Id == _vpnActiveId ? "  [ACTIVE]" : "");
            var item = new ListBoxItem { Content = label, Tag = p.Id, Foreground = Brushes.White };
            LstVpnProfiles.Items.Add(item);
            if (p.Id == selectId) toSelect = item;
        }
        if (toSelect != null) LstVpnProfiles.SelectedItem = toSelect;

        RefreshVpnCountryPicker();
    }

    private void RefreshVpnCountryPicker()
    {
        var selected = CboVpnCountryPicker.SelectedItem as string ?? "";
        var countries = VpnProfileStore.Profiles
            .Select(p => (p.Country ?? "").Trim().ToUpperInvariant())
            .Where(c => c.Length == 2)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        CboVpnCountryPicker.ItemsSource = countries;
        if (countries.Contains(selected)) CboVpnCountryPicker.SelectedItem = selected;
        else if (countries.Count > 0) CboVpnCountryPicker.SelectedIndex = 0;

        BtnVpnQuickConnect.IsEnabled = countries.Count > 0;
    }

    private async void BtnVpnQuickConnect_Click(object sender, RoutedEventArgs e)
    {
        var country = CboVpnCountryPicker.SelectedItem as string;
        if (string.IsNullOrEmpty(country))
        {
            SetVpnStatus("No countries available.", false);
            return;
        }

        var candidates = VpnProfileStore.Profiles
            .Where(p => string.Equals((p.Country ?? "").Trim(), country, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            SetVpnStatus($"No saved server found for {country}.", false);
            return;
        }

        var profile = candidates[Random.Shared.Next(candidates.Count)];
        BtnVpnQuickConnect.IsEnabled = false;
        SetVpnStatus($"Connecting to {profile.Name} ({country})...", null);
        try
        {
            var (ok, info) = await VpnRelayService.ConnectAsync(profile);
            if (ok)
            {
                _vpnActiveId = profile.Id;
                var s = SettingsService.Current;
                s.VpnEnabled = true;
                s.VpnActiveProfileId = profile.Id;
                SettingsService.Save();
                RefreshVpnList(_vpnActiveId);
                ChkVpnEnabled.IsChecked = true;
                SetVpnStatus($"Connected via {profile.Name}, exit IP {info}", true);
            }
            else
            {
                SetVpnStatus($"Failed: {info}", false);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnControlCenterWindow.QuickConnect");
            SetVpnStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            BtnVpnQuickConnect.IsEnabled = true;
        }
    }

    private void ClearVpnEditor()
    {
        _vpnEditingId = "";
        TxtVpnName.Text = "New Server";
        TxtVpnHost.Text = "";
        TxtVpnPort.Text = "";
        TxtVpnUsername.Text = "";
        PwdVpn.Password = "";
        TxtVpnCountry.Text = "";
        CboVpnType.SelectedIndex = 0;
    }

    private void LstVpnProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstVpnProfiles.SelectedItem is not ListBoxItem item) return;
        var id = item.Tag as string;
        var p = VpnProfileStore.GetById(id);
        if (p == null) return;

        _vpnEditingId = p.Id;
        TxtVpnName.Text = p.Name;
        TxtVpnHost.Text = p.Host;
        TxtVpnPort.Text = p.Port.ToString();
        TxtVpnUsername.Text = p.Username;
        PwdVpn.Password = p.Password;
        TxtVpnCountry.Text = p.Country ?? "";
        foreach (ComboBoxItem cti in CboVpnType.Items)
        {
            if ((string)cti.Tag == p.Type) { CboVpnType.SelectedItem = cti; break; }
        }
    }

    private bool TryReadVpnEditor(out VpnProfile profile, out string error)
    {
        var existing = string.IsNullOrEmpty(_vpnEditingId) ? null : VpnProfileStore.GetById(_vpnEditingId);
        profile = new VpnProfile
        {
            Id       = string.IsNullOrEmpty(_vpnEditingId) ? Guid.NewGuid().ToString("N") : _vpnEditingId,
            Name     = TxtVpnName.Text.Trim(),
            Type     = (CboVpnType.SelectedItem as ComboBoxItem)?.Tag as string ?? "Http",
            Host     = TxtVpnHost.Text.Trim(),
            Port     = int.TryParse(TxtVpnPort.Text.Trim(), out var port) ? port : 0,
            Username = TxtVpnUsername.Text.Trim(),
            Password = PwdVpn.Password,
            Source   = existing?.Source ?? "Custom",
            Country  = TxtVpnCountry.Text.Trim()
        };
        return VpnProfileStore.Validate(profile, out error);
    }

    private void BtnVpnNew_Click(object sender, RoutedEventArgs e) => ClearVpnEditor();

    private void BtnVpnSaveServer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadVpnEditor(out var profile, out var error))
        {
            SetVpnStatus(error, false);
            return;
        }

        VpnProfileStore.Save(profile);
        _vpnEditingId = profile.Id;
        RefreshVpnList(profile.Id);
        SetVpnStatus("Server saved.", true);
    }

    private void BtnVpnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vpnEditingId) || VpnProfileStore.GetById(_vpnEditingId) == null)
        {
            SetVpnStatus("Select a server to delete.", false);
            return;
        }

        string id = _vpnEditingId;
        VpnProfileStore.Delete(id);
        if (_vpnActiveId == id) _vpnActiveId = "";
        ClearVpnEditor();
        RefreshVpnList(null);
        SetVpnStatus("Server deleted.", true);
    }

    private void BtnVpnUse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vpnEditingId) || VpnProfileStore.GetById(_vpnEditingId) == null)
        {
            SetVpnStatus("Save the server first, then select it.", false);
            return;
        }

        _vpnActiveId = _vpnEditingId;
        SaveVpnSettings();
        RefreshVpnList(_vpnActiveId);
        SetVpnStatus("Active server set.", true);
    }

    private async void BtnVpnTest_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadVpnEditor(out var profile, out var error))
        {
            SetVpnStatus(error, false);
            return;
        }

        BtnVpnTest.IsEnabled = false;
        SetVpnStatus("Testing connection...", null);
        try
        {
            var (ok, info) = await VpnRelayService.TestProfileAsync(profile);
            SetVpnStatus(ok ? $"OK. Exit IP: {info}" : $"Failed: {info}", ok);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnControlCenterWindow.VpnTest");
            SetVpnStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            BtnVpnTest.IsEnabled = true;
        }
    }

    private void SetVpnStatus(string message, bool? ok)
    {
        TblVpnStatus.Text = message;
        TblVpnStatus.Foreground = ok == true ? Brushes.LightGreen : ok == false ? Brushes.OrangeRed : Brushes.Gray;
    }

    // ── AdGuard section ─────────────────────────────────────────────────────

    private void BuildAdGuardSection()
    {
        var s = SettingsService.Current;
        ChkAdGuardEnabled.IsChecked = s.AdGuardEnabled;
        ChkAdGuardEnabled.Checked   += (_, _) => { s.AdGuardEnabled = true; SettingsService.Save(); };
        ChkAdGuardEnabled.Unchecked += (_, _) => { s.AdGuardEnabled = false; SettingsService.Save(); };

        PnlAdGuardFilters.Children.Add(AdGuardRow("Block Ads", s.AdGuard_BlockAds, v => s.AdGuard_BlockAds = v));
        PnlAdGuardFilters.Children.Add(AdGuardRow("Block Trackers", s.AdGuard_BlockTrackers, v => s.AdGuard_BlockTrackers = v));
        PnlAdGuardFilters.Children.Add(AdGuardRow("Block Annoyances", s.AdGuard_BlockAnnoyances, v => s.AdGuard_BlockAnnoyances = v));
        PnlAdGuardFilters.Children.Add(AdGuardRow("Social Widgets", s.AdGuard_SocialWidgets, v => s.AdGuard_SocialWidgets = v));
    }

    private static CheckBox AdGuardRow(string label, bool value, Action<bool> setter)
    {
        var chk = new CheckBox { Content = label, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 0, 4), IsChecked = value };
        chk.Checked   += (_, _) => { setter(true);  SettingsService.Save(); };
        chk.Unchecked += (_, _) => { setter(false); SettingsService.Save(); };
        return chk;
    }

    // ── Throughput history ──────────────────────────────────────────────────

    private void DrawThroughputHistory()
    {
        var samples = VpnThroughputHistoryService.GetRecentSamples();
        ThroughputCanvas.Children.Clear();

        if (samples.Count < 2)
        {
            TblThroughputEmpty.Visibility = Visibility.Visible;
            TblThroughputCurrent.Text = "";
            return;
        }
        TblThroughputEmpty.Visibility = Visibility.Collapsed;

        double w = ThroughputCanvas.ActualWidth > 0 ? ThroughputCanvas.ActualWidth : 800;
        double h = ThroughputCanvas.ActualHeight > 0 ? ThroughputCanvas.ActualHeight : 140;
        double maxBps = Math.Max(1, samples.Max(s => s.Bps));

        var poly = new Polyline { Stroke = Brushes.MediumSeaGreen, StrokeThickness = 2 };
        for (int i = 0; i < samples.Count; i++)
        {
            double x = w * i / (samples.Count - 1);
            double y = h - (samples[i].Bps / maxBps * (h - 10)) - 5;
            poly.Points.Add(new Point(x, y));
        }
        ThroughputCanvas.Children.Add(poly);

        var lastBits = samples[^1].Bps * 8.0;
        TblThroughputCurrent.Text = lastBits >= 1_000_000
            ? $"{lastBits / 1_000_000:0.0} Mbps"
            : $"{lastBits / 1_000:0} Kbps";
    }
}