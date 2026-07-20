using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;
using Horizon.Stealth.Services;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;


namespace Horizon.Stealth.Views;

public partial class SettingsWindow : Window
    {
        private static readonly string StartVideosFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "start_videos");

        public class VideoEntry : INotifyPropertyChanged
        {
            public string Name { get; set; } = "";
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private ObservableCollection<VideoEntry> _customVideos = new();
        private bool _suppressVideoOptionEvents = false;

        private IEnumerable<string> GetAvailableVideoFiles()
        {
            try
            {
                if (!Directory.Exists(StartVideosFolder)) return Enumerable.Empty<string>();
                return Directory.GetFiles(StartVideosFolder, "*.*")
                    .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)!;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "GetAvailableVideoFiles");
                return Enumerable.Empty<string>();
            }
        }

        private void LoadStartupVideoOptions()
        {
            _suppressVideoOptionEvents = true;
            var s = SettingsService.Current;

            var files = GetAvailableVideoFiles().ToList();

            CboStartupVideoFile.Items.Clear();
            foreach (var f in files) CboStartupVideoFile.Items.Add(f);
            if (!string.IsNullOrEmpty(s.StartupVideoFileName) && CboStartupVideoFile.Items.Contains(s.StartupVideoFileName))
                CboStartupVideoFile.SelectedItem = s.StartupVideoFileName;
            else if (CboStartupVideoFile.Items.Count > 0)
                CboStartupVideoFile.SelectedIndex = 0;

            _customVideos = new ObservableCollection<VideoEntry>(
                files.Select(f => new VideoEntry { Name = f, IsSelected = s.StartupVideoCustomList.Contains(f) }));
            LstCustomVideos.ItemsSource = _customVideos;

            SelectComboBoxByTag(CboVideoPlayOrder, string.IsNullOrEmpty(s.StartupVideoOrder) ? "Sequential" : s.StartupVideoOrder);

            switch (s.StartupVideoMode)
            {
                case "Random": RbVideoModeRandom.IsChecked = true; break;
                case "Custom": RbVideoModeCustom.IsChecked = true; break;
                default: RbVideoModeFixed.IsChecked = true; break;
            }

            UpdateStartupVideoOptionsVisibility();
            _suppressVideoOptionEvents = false;
        }

        private void UpdateStartupVideoOptionsVisibility()
        {
            bool enabled = ChkStartupVideo.IsChecked == true;
            PnlStartupVideoOptions.IsEnabled = enabled;

            CboStartupVideoFile.Visibility = RbVideoModeFixed.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LstCustomVideos.Visibility = RbVideoModeCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CboVideoPlayOrder.Visibility = RbVideoModeCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveStartupVideoOptions()
        {
            var s = SettingsService.Current;

            s.StartupVideoMode = RbVideoModeRandom.IsChecked == true ? "Random"
                                : RbVideoModeCustom.IsChecked == true ? "Custom"
                                : "Fixed";

            if (CboStartupVideoFile.SelectedItem is string selectedFile)
                s.StartupVideoFileName = selectedFile;

            s.StartupVideoCustomList = _customVideos.Where(v => v.IsSelected).Select(v => v.Name).ToList();

            if (CboVideoPlayOrder.SelectedItem is ComboBoxItem oi)
                s.StartupVideoOrder = oi.Tag?.ToString() ?? "Sequential";
        }

        private void ChkStartupVideo_Toggled(object sender, RoutedEventArgs e) => UpdateStartupVideoOptionsVisibility();

        private void VideoMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressVideoOptionEvents) return;
            UpdateStartupVideoOptionsVisibility();
        }

        private void CboStartupVideoFile_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void CboVideoPlayOrder_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void CustomVideoCheck_Changed(object sender, RoutedEventArgs e) { }

        private void BtnOpenVideosFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(StartVideosFolder)) Directory.CreateDirectory(StartVideosFolder);
                Process.Start(new ProcessStartInfo { FileName = StartVideosFolder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "BtnOpenVideosFolder_Click");
            }
        }

        private void BtnChangeStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "Video files (*.mp4;*.wmv;*.mov)|*.mp4;*.wmv;*.mov|All files (*.*)|*.*",
                    Title = "Select a startup video"
                };
                if (dlg.ShowDialog() != true) return;

                if (!Directory.Exists(StartVideosFolder)) Directory.CreateDirectory(StartVideosFolder);

                string destFileName = Path.GetFileName(dlg.FileName);
                string destPath = Path.Combine(StartVideosFolder, destFileName);

                if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(dlg.FileName, destPath, overwrite: true);

                LoadStartupVideoOptions();
                CboStartupVideoFile.SelectedItem = destFileName;
                RbVideoModeFixed.IsChecked = true;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "BtnChangeStartupVideo_Click");
                MessageBox.Show("Could not add the video.\n" + ex.Message);
            }
        }
    public event Action? SettingsApplied;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadState();
        LoadVersionDisplay();
    }

    private void TitleBarRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnSettingsMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnSettingsMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            WindowState = WindowState.Maximized;
        }
    }

    private void BtnSettingsClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool _isRestoringTab = true;
    private bool _isLoadingVersion = false;

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Clamp growth to the visible work area (excludes the taskbar), minus a
        // roughly "one finger width" margin (~60px) on each dimension so the
        // window never grows edge-to-edge or gets its bottom buttons hidden
        // behind the taskbar.
        const double edgeMargin = 60.0;

        double workAreaWidth  = SystemParameters.WorkArea.Width;
        double workAreaHeight = SystemParameters.WorkArea.Height;

        MaxWidth  = Math.Max(MinWidth, workAreaWidth  - edgeMargin);
        MaxHeight = workAreaHeight - edgeMargin;

        int savedIndex = SettingsService.Current.LastSettingsTabIndex;
        if (savedIndex >= 0 && savedIndex < TabsMain.Items.Count)
            TabsMain.SelectedIndex = savedIndex;

        _isRestoringTab = false;

        }

    private void TabsMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringTab) return;
        if (sender != TabsMain) return; // ignore bubbled events from nested controls, if any

        SettingsService.Current.LastSettingsTabIndex = TabsMain.SelectedIndex;
        SettingsService.Save();
    }

    private void LoadVersionDisplay()
    {
        _isLoadingVersion = true;

        string? versionPath = ResolvePath("version.txt");
        if (versionPath == null)
        {
            TblVersionDisplay.Text = "VERSION UNKNOWN";
            SetComboByTag(CboUpdateChannel, "release");
            _isLoadingVersion = false;
            return;
        }

        string raw;
        try { raw = File.ReadAllText(versionPath).Trim(); }
        catch
        {
            TblVersionDisplay.Text = "VERSION UNREADABLE";
            SetComboByTag(CboUpdateChannel, "release");
            _isLoadingVersion = false;
            return;
        }

        string normalized = ParseVersion(raw);
        string channel = ParseChannel(raw);

        TblVersionDisplay.Text = string.IsNullOrEmpty(channel)
            ? "VERSION " + normalized
            : "VERSION " + normalized + " " + channel.ToUpperInvariant() + " ";

        SetComboByTag(CboUpdateChannel, channel);
        _isLoadingVersion = false;
    }

    private void CboUpdateChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingVersion) return;

        string channel = GetComboTag(CboUpdateChannel);
        if (string.IsNullOrEmpty(channel)) return;

        string? versionPath = ResolvePath("version.txt");
        string version = "0.0.0";

        if (versionPath != null)
        {
            try
            {
                string raw = File.ReadAllText(versionPath).Trim();
                string parsed = ParseVersion(raw);
                if (parsed != "UNKNOWN") version = parsed;
            }
            catch { }
        }
        else
        {
            versionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
        }

        try
        {
            File.WriteAllText(versionPath, $"version {version}\n{channel}");
            LoadVersionDisplay();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Writing version.txt channel");
        }
    }

    private static string ParseVersion(string raw)
    {
        string[] lines = raw.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            string candidate = trimmed;

            if (trimmed.StartsWith("version", StringComparison.OrdinalIgnoreCase))
                candidate = trimmed.Substring(7).Trim();

            if (string.IsNullOrEmpty(candidate)) continue;

            string[] parts = candidate.Split('.');
            bool allNumeric = true;
            foreach (string p in parts)
                if (!int.TryParse(p.Trim(), out _)) { allNumeric = false; break; }

            if (!allNumeric) continue;

            if (parts.Length == 1)
                return parts[0].Trim() + ".0";

            return string.Join(".", Array.ConvertAll(parts, p => p.Trim()));
        }

        return "UNKNOWN";
    }

    private static readonly string[] _knownChannels = { "alpha", "beta", "rc", "release", "stable" };

    private static string ParseChannel(string raw)
    {
        string[] lines = raw.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string[] words = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in words)
            {
                string wordClean = word.Trim().ToLowerInvariant();
                foreach (string known in _knownChannels)
                {
                    if (wordClean == known)
                        return known;
                }
            }
        }

        return "release";
    }

    private static readonly string[] _knownChannelsSyncNote = _knownChannels;

    // ── Path helpers (unchanged from original) ────────────────────────────────

    private List<string> GetSearchPaths()
    {
        var paths = new List<string>();
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        paths.Add(currentDir);

        string? traversalDir = currentDir;
        while (traversalDir != null)
        {
            if (File.Exists(Path.Combine(traversalDir, "start_mapper.bat")))
            {
                if (!paths.Contains(traversalDir)) paths.Add(traversalDir);
                break;
            }
            traversalDir = Directory.GetParent(traversalDir)?.FullName;
        }
        return paths;
    }

    private string? ResolvePath(string filename)
    {
        foreach (var dir in GetSearchPaths())
        {
            string full = Path.Combine(dir, filename);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    // ── SponsorBlock visuals (unchanged) ─────────────────────────────────────

    private void CboSearch_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PanelCustomSearch == null) return;
        bool isCustom = (CboSearch.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "custom";
        PanelCustomSearch.Visibility = isCustom ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void UpdateSponsorBlockVisuals()
    {
        if (PanelSponsorBlockOpts == null) return;
        bool on = ChkSponsorBlock.IsChecked == true;
        PanelSponsorBlockOpts.IsEnabled = on;
        PanelSponsorBlockOpts.Opacity   = on ? 1.0 : 0.4;
    }

    private void ChkSponsorBlock_CheckedChanged(object sender, RoutedEventArgs e)
        => UpdateSponsorBlockVisuals();

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadState()
    {
        var s = SettingsService.Current;

        TxtHome.Text = s.HomePage;
        // Match saved URL against the built-in tag values; fall back to Custom
        bool foundEngine = false;
        foreach (ComboBoxItem item in CboSearch.Items)
        {
            if (item.Tag?.ToString() == s.SearchEngineUrl)
            {
                CboSearch.SelectedItem = item;
                foundEngine = true;
                break;
            }
        }
        if (!foundEngine)
        {
            // Select "Custom…" and populate the URL box
            foreach (ComboBoxItem item in CboSearch.Items)
                if (item.Tag?.ToString() == "custom") { CboSearch.SelectedItem = item; break; }
            TxtCustomSearchUrl.Text = s.SearchEngineUrl;
            PanelCustomSearch.Visibility = System.Windows.Visibility.Visible;
        }
        SetComboByTag(CboTheme, string.IsNullOrEmpty(s.Theme) ? "Horizon" : s.Theme);

		SelectComboBoxByTag(CboTabTitleMode, string.IsNullOrEmpty(s.TabTitleMode) ? "Full" : s.TabTitleMode);
        SelectComboBoxByTag(CboVisualizerScheme, string.IsNullOrEmpty(s.VisualizerColorScheme) ? "Favicon" : s.VisualizerColorScheme);
        SliderPaletteSampleRate.Value    = s.PaletteSampleRateSec > 0 ? s.PaletteSampleRateSec : 1.5;
        TblPaletteSampleRateValue.Text   = $"{SliderPaletteSampleRate.Value:F1} s";

        ChkStartupVideo.IsChecked = s.ShowStartupVideo;
        LoadStartupVideoOptions();
		
        ChkHideHeader.IsChecked     = s.AutoHideHeader;
        ChkHideSidebar.IsChecked    = s.AutoHideSidebar;
        ChkSessionRestore.IsChecked      = s.ShowSessionRestore;
        ChkAutoRestoreSession.IsChecked  = s.AutoRestoreSession;
        ChkBackgroundKeepAlive.IsChecked = s.BackgroundKeepAliveEnabled;
        ChkStartOnSystemStartup.IsChecked = s.StartOnSystemStartup;
        ChkSilentUpdateCheck.IsChecked     = s.SilentUpdateCheckEnabled;
        ChkAutoDownloadUpdates.IsChecked   = s.AutoDownloadUpdatesEnabled;
        ChkStealth.IsChecked              = s.IsStealthMode;
        ChkSleepingTabsEnabled.IsChecked  = s.SleepingTabsEnabled;

        ChkSponsorBlock.IsChecked  = s.EnableSponsorBlock;
        ChkSbSponsors.IsChecked    = s.SB_Sponsors;
        ChkSbSelfPromo.IsChecked   = s.SB_SelfPromo;
        ChkSbIntro.IsChecked       = s.SB_Intro;
        ChkSbOutro.IsChecked       = s.SB_Outro;
        ChkSbInteraction.IsChecked = s.SB_Interaction;
        ChkSbMusic.IsChecked       = s.SB_MusicOfftopic;
        UpdateSponsorBlockVisuals();

        ChkHideSponsoredResults.IsChecked = s.HideSponsoredResults;

        TxtDns.Text = s.NextDnsId;

        TxtJanitorSmall.Text = s.JanitorSmallFileMb.ToString();
        TxtJanitorLarge.Text = s.JanitorLargeFileGb.ToString();
        TxtJanitorTime.Text  = s.JanitorRetentionMin.ToString();

        ChkPerTabLang.IsChecked = s.PerTabLanguageEnabled;
        SelectComboBoxByTag(CboDefaultLanguage, s.DefaultLanguage);

        TxtMicrosoftClientId.Text  = s.MicrosoftClientId;

        SliderScrollSpeed.Value = s.ScrollSpeedMultiplier;
        TblScrollSpeedValue.Text = $"{s.ScrollSpeedMultiplier:F2}×";

        TxtTabDefaultWidth.Text      = ((int)s.TabDefaultWidth).ToString();
        TxtTabMediaWidth.Text        = ((int)s.TabMediaPlaybackWidth).ToString();
        TxtTabDownloadWidth.Text     = ((int)s.TabDownloadModeWidth).ToString();
        TxtTabMediaHoverWidth.Text   = ((int)s.MediaTabHoverWidth).ToString();
        TxtTabDownloadHoverWidth.Text = ((int)s.DownloadTabHoverWidth).ToString();

        CmbNarrowWindowMode.SelectedIndex = s.NarrowWindowMode;
        TxtNarrowThreshold.Text = s.NarrowWindowThresholdPx.ToString();

        LoadDefaultGoogleAccountCombo();

        // Build dynamic extension cards (replaces the old ChkConsentOMatic / ChkAdGuard)
        BuildInstalledExtensionsList();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Current;

        s.HomePage = TxtHome.Text;
        if (CboSearch.SelectedItem is ComboBoxItem si)
        {
            string tag = si.Tag?.ToString() ?? "";
            if (tag == "custom")
            {
                s.SearchEngineUrl = TxtCustomSearchUrl.Text.Trim();
                s.SearchEngine    = "Custom";
            }
            else
            {
                s.SearchEngineUrl = tag;
                s.SearchEngine    = si.Content.ToString() ?? "AlohFind";
            }
        }
        s.Theme = GetComboTag(CboTheme);
        ThemeService.ApplyTheme(s.Theme);

        if (CboTabTitleMode.SelectedItem is ComboBoxItem tmi)
            s.TabTitleMode = tmi.Tag?.ToString() ?? "Full";

        if (CboVisualizerScheme.SelectedItem is ComboBoxItem vci)
            s.VisualizerColorScheme = vci.Tag?.ToString() ?? "Favicon";
        s.PaletteSampleRateSec = SliderPaletteSampleRate.Value;

        s.ShowStartupVideo = ChkStartupVideo.IsChecked == true;
        SaveStartupVideoOptions();

        s.AutoHideHeader     = ChkHideHeader.IsChecked     == true;
        s.AutoHideSidebar    = ChkHideSidebar.IsChecked    == true;
        s.ShowSessionRestore  = ChkSessionRestore.IsChecked     == true;
        s.AutoRestoreSession        = ChkAutoRestoreSession.IsChecked  == true;
        s.BackgroundKeepAliveEnabled = ChkBackgroundKeepAlive.IsChecked == true;
        BackgroundKeepAliveService.OnSettingChanged();
        s.StartOnSystemStartup = ChkStartOnSystemStartup.IsChecked == true;
        StartupService.Apply(s.StartOnSystemStartup);
        s.SilentUpdateCheckEnabled    = ChkSilentUpdateCheck.IsChecked   == true;
        s.AutoDownloadUpdatesEnabled  = ChkAutoDownloadUpdates.IsChecked == true;
        s.IsStealthMode       = ChkStealth.IsChecked             == true;
        s.SleepingTabsEnabled = ChkSleepingTabsEnabled.IsChecked == true;

        s.EnableSponsorBlock  = ChkSponsorBlock.IsChecked  == true;
        s.SB_Sponsors         = ChkSbSponsors.IsChecked    == true;
        s.SB_SelfPromo        = ChkSbSelfPromo.IsChecked   == true;
        s.SB_Intro            = ChkSbIntro.IsChecked       == true;
        s.SB_Outro            = ChkSbOutro.IsChecked       == true;
        s.SB_Interaction      = ChkSbInteraction.IsChecked == true;
        s.SB_MusicOfftopic    = ChkSbMusic.IsChecked       == true;
        s.HideSponsoredResults = ChkHideSponsoredResults.IsChecked == true;

        s.NextDnsId = TxtDns.Text;

        int.TryParse(TxtJanitorSmall.Text, out int jS);
        int.TryParse(TxtJanitorLarge.Text, out int jL);
        int.TryParse(TxtJanitorTime.Text,  out int jT);
        s.JanitorSmallFileMb  = jS;
        s.JanitorLargeFileGb  = jL;
        s.JanitorRetentionMin = jT;

        s.PerTabLanguageEnabled = ChkPerTabLang.IsChecked == true;
        if (CboDefaultLanguage.SelectedItem is ComboBoxItem li)
            s.DefaultLanguage = li.Tag?.ToString() ?? "en";

        s.ScrollSpeedMultiplier = SliderScrollSpeed.Value;

        if (double.TryParse(TxtTabDefaultWidth.Text,      out double twD) && twD >= 90  && twD <= 400) s.TabDefaultWidth       = twD;
        if (double.TryParse(TxtTabMediaWidth.Text,        out double twM) && twM >= 90  && twM <= 400) s.TabMediaPlaybackWidth = twM;
        if (double.TryParse(TxtTabDownloadWidth.Text,     out double twDl) && twDl >= 90 && twDl <= 400) s.TabDownloadModeWidth = twDl;
        if (double.TryParse(TxtTabMediaHoverWidth.Text,   out double twMH) && twMH >= 120 && twMH <= 500) s.MediaTabHoverWidth   = twMH;
        if (double.TryParse(TxtTabDownloadHoverWidth.Text,out double twDH) && twDH >= 120 && twDH <= 500) s.DownloadTabHoverWidth = twDH;

        s.NarrowWindowMode = CmbNarrowWindowMode.SelectedIndex;
        if (int.TryParse(TxtNarrowThreshold.Text, out int nwt) && nwt >= 400)
            s.NarrowWindowThresholdPx = nwt;

        if (CboDefaultGoogleAccount.SelectedItem is ComboBoxItem dgai)
            s.DefaultGoogleAccountEmail = dgai.Tag?.ToString() ?? "";

        s.MicrosoftClientId  = TxtMicrosoftClientId.Text.Trim();

        SettingsService.Save();
        FluxJanitorService.Initialize();

        MessageBox.Show("Configuration Saved.", "Horizon", MessageBoxButton.OK, MessageBoxImage.Information);
        SettingsApplied?.Invoke();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { Close(); }

    private static void SetComboByTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (item.Tag?.ToString() ?? "") == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Dark";

    

    // ── Default Browser ───────────────────────────────────────────────────────

    private void BtnSetDefaultBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "Horizon.Stealth.exe");
            RegisterAsDefaultBrowser(exePath);

            // Open Windows Default Apps settings — user makes the final selection there
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "SetDefaultBrowser");
            MessageBox.Show($"Could not register as default browser:\n{ex.Message}",
                "Horizon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RegisterAsDefaultBrowser(string exePath)
    {
        const string progId  = "HorizonStealth.HTML";
        const string appName = "Horizon Browser";
        const string appKey  = @"Software\Clients\StartMenuInternet\HorizonBrowser";
        const string capsKey = @"Software\Horizon Browser\Capabilities";

        // ── 1. ProgID: what Windows invokes when opening http/https/html ──────
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
        {
            key.SetValue("", appName + " Document");
            key.SetValue("FriendlyTypeName", appName + " Document");
        }
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon"))
            key.SetValue("", $"\"{exePath}\",0");
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
            key.SetValue("", $"\"{exePath}\" \"%1\"");

        // ── 2. StartMenuInternet registration (shows in "Default apps" chooser) ─
        using (var key = Registry.CurrentUser.CreateSubKey(appKey))
            key.SetValue("", appName);
        using (var key = Registry.CurrentUser.CreateSubKey($@"{appKey}\DefaultIcon"))
            key.SetValue("", $"\"{exePath}\",0");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{appKey}\shell\open\command"))
            key.SetValue("", $"\"{exePath}\"");
        // Recommended start URL shown in Default Apps UI
        using (var key = Registry.CurrentUser.CreateSubKey($@"{appKey}\shell\open\command"))
            key.SetValue("", $"\"{exePath}\"");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{appKey}\Capabilities"))
        {
            key.SetValue("ApplicationName",        appName);
            key.SetValue("ApplicationDescription", "Privacy-focused Stealth Browser");
            key.SetValue("ApplicationIcon",        $"\"{exePath}\",0");
            using (var url = key.CreateSubKey("URLAssociations"))
            {
                url.SetValue("http",  progId);
                url.SetValue("https", progId);
                url.SetValue("ftp",   progId);
            }
            using (var fa = key.CreateSubKey("FileAssociations"))
            {
                fa.SetValue(".htm",   progId);
                fa.SetValue(".html",  progId);
                fa.SetValue(".xhtml", progId);
                fa.SetValue(".xht",   progId);
            }
        }

        // ── 3. Capabilities (RegisteredApplications path) ─────────────────────
        using (var caps = Registry.CurrentUser.CreateSubKey(capsKey))
        {
            caps.SetValue("ApplicationName",        appName);
            caps.SetValue("ApplicationDescription", "Privacy-focused Stealth Browser");
            caps.SetValue("ApplicationIcon",        $"\"{exePath}\",0");
            using (var url = caps.CreateSubKey("URLAssociations"))
            {
                url.SetValue("http",  progId);
                url.SetValue("https", progId);
                url.SetValue("ftp",   progId);
            }
            using (var fa = caps.CreateSubKey("FileAssociations"))
            {
                fa.SetValue(".htm",   progId);
                fa.SetValue(".html",  progId);
            }
            using (var mimes = caps.CreateSubKey("MIMEAssociations"))
            {
                mimes.SetValue("text/html",             progId);
                mimes.SetValue("application/xhtml+xml", progId);
            }
        }

        // ── 4. RegisteredApplications pointer ─────────────────────────────────
        using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            regApps.SetValue(appName, capsKey);

        LogService.Write("SETTINGS", "Registered Horizon as candidate default browser.");
    }

    // ── Extensions tab: dynamic card list ────────────────────────────────────

    /// <summary>
    /// Builds one card per installed extension. Each card shows:
    ///   icon | name | version/source | ON/OFF toggle | Uninstall button
    ///   └── expandable settings panel with extension-specific options
    /// Called from LoadState() and after any uninstall action.
    /// </summary>
    private void BuildInstalledExtensionsList()
    {
        if (PanelInstalledExtensions == null) return;
        PanelInstalledExtensions.Children.Clear();

        var extensions = ExtensionService.All;

        if (!extensions.Any())
        {
            PanelInstalledExtensions.Children.Add(new TextBlock
            {
                Text       = "No extensions installed yet.\n\nUse the sidebar → 🧩 tab while browsing a Chrome, Edge, or Firefox extension store page to install one.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontSize   = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin     = new Thickness(0, 4, 0, 0),
            });
            return;
        }

        foreach (var ext in extensions)
        {
            bool isBundled = ext.Source == ExtensionSource.Bundled;

            // ── Outer card ────────────────────────────────────────────────────
            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                BorderBrush     = ExtBorderBrush(ext.Enabled),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Margin          = new Thickness(0, 0, 0, 8),
                Padding         = new Thickness(0),
            };

            var outer = new StackPanel();

            // ── Header row: icon | name+badge | [ON/OFF] [🗑] ────────────────
            var header = new Grid { Margin = new Thickness(12, 9, 12, 9) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition());
            header.RowDefinitions.Add(new RowDefinition());

            var iconTb = new TextBlock { Text = ext.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(iconTb, 0); Grid.SetRowSpan(iconTb, 2);

            var nameTb = new TextBlock
            {
                Text = ext.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.WhiteSmoke, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(nameTb, 1); Grid.SetRow(nameTb, 0);

            string srcLabel = ext.Source switch
            {
                ExtensionSource.Bundled      => "bundled",
                ExtensionSource.ChromeStore  => "Chrome Web Store",
                ExtensionSource.EdgeStore    => "Edge Add-ons",
                ExtensionSource.FirefoxStore => "Firefox AMO",
                _                            => "manual",
            };
            string vLabel = string.IsNullOrEmpty(ext.Version) ? srcLabel : $"v{ext.Version}  ·  {srcLabel}";
            var verTb = new TextBlock
            {
                Text = vLabel, FontSize = 9, Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x33)),
            };
            Grid.SetColumn(verTb, 1); Grid.SetRow(verTb, 1);

            // Buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(btnRow, 2); Grid.SetRowSpan(btnRow, 2);

            var toggleBtn = new Button
            {
                Content = ext.Enabled ? "ON" : "OFF",
                Width = 48, Height = 24, FontSize = 9, FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
            };
            ApplyToggleStyle(toggleBtn, ext.Enabled);

            var captExt  = ext;
            var captCard = card;
            toggleBtn.Click += (_, _) =>
            {
                bool ns = !captExt.Enabled;
                ExtensionService.SetEnabled(captExt.Id, ns);
                toggleBtn.Content       = ns ? "ON" : "OFF";
                captCard.BorderBrush    = ExtBorderBrush(ns);
                ApplyToggleStyle(toggleBtn, ns);
                BannerSettingsRestart.Visibility = Visibility.Visible;
            };

            var uninstBtn = new Button
            {
                Content = isBundled ? "✕ Disable" : "🗑 Remove",
                Height = 24, FontSize = 9, Padding = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x00, 0x00)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x22, 0x22)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x11, 0x11)),
                ToolTip = isBundled ? "Bundled extensions cannot be fully removed — they will be disabled instead." : "Permanently uninstall this extension.",
            };
            uninstBtn.Click += (_, _) =>
            {
                string verb = isBundled ? "disable" : "uninstall";
                if (MessageBox.Show($"Are you sure you want to {verb} \"{captExt.Name}\"?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                ExtensionService.Uninstall(captExt.Id);
                BannerSettingsRestart.Visibility = Visibility.Visible;
                BuildInstalledExtensionsList();
            };

            btnRow.Children.Add(toggleBtn);
            btnRow.Children.Add(uninstBtn);

            header.Children.Add(iconTb);
            header.Children.Add(nameTb);
            header.Children.Add(verTb);
            header.Children.Add(btnRow);
            outer.Children.Add(header);

            // ── Description ───────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(ext.Description))
            {
                outer.Children.Add(new TextBlock
                {
                    Text = ext.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 0, 12, 8),
                });
            }

            // ── Per-extension settings panel ──────────────────────────────────
            var settingsPanel = BuildExtensionSettingsPanel(ext);
            if (settingsPanel != null)
            {
                outer.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Fill   = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    Margin = new Thickness(0),
                });
                outer.Children.Add(settingsPanel);
            }

            card.Child = outer;
            PanelInstalledExtensions.Children.Add(card);
        }
    }

    /// <summary>
    /// Returns an extension-specific settings StackPanel, or null if the extension
    /// has no configurable options we surface here.
    /// </summary>
    private UIElement? BuildExtensionSettingsPanel(ExtensionRecord ext)
    {
        string id = ext.Id.ToLowerInvariant();

        // StackPanel has no Padding — wrap in a Border instead.
        var panelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)),
            Padding    = new Thickness(12, 8, 12, 10),
        };
        var panel = new StackPanel { Margin = new Thickness(0) };
        panelBorder.Child = panel;

        var header = new TextBlock
        {
            Text = "Settings",
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x33)),
            FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        panel.Children.Add(header);

        if (id == "adguard")
        {
            panel.Children.Add(SettingsNote("AdGuard runs automatically when enabled. Configure filter lists and custom rules inside the AdGuard popup (click its icon in the browser toolbar)."));
            panel.Children.Add(SettingsRow("Block Ads",       "Removes banner, video, and pop-up ads.",            "AdGuard_BlockAds",       SettingsService.Current.AdGuard_BlockAds,       v => SettingsService.Current.AdGuard_BlockAds       = v));
            panel.Children.Add(SettingsRow("Block Trackers",  "Prevents cross-site tracking scripts.",             "AdGuard_BlockTrackers",  SettingsService.Current.AdGuard_BlockTrackers,  v => SettingsService.Current.AdGuard_BlockTrackers  = v));
            panel.Children.Add(SettingsRow("Block Annoyances","Hides cookie banners and newsletter pop-ups.",      "AdGuard_BlockAnnoyances",SettingsService.Current.AdGuard_BlockAnnoyances, v => SettingsService.Current.AdGuard_BlockAnnoyances = v));
            panel.Children.Add(SettingsRow("Social Widgets",  "Removes embedded social share/like buttons.",       "AdGuard_SocialWidgets",  SettingsService.Current.AdGuard_SocialWidgets,  v => SettingsService.Current.AdGuard_SocialWidgets  = v));
            panel.Children.Add(SettingsNote("Filter list changes take effect immediately. Restart not required."));
            return panelBorder;
        }

        if (id == "consent-o-matic")
        {
            panel.Children.Add(SettingsNote("Consent-O-Matic automatically answers GDPR/cookie dialogs. The options below control the default answer for each consent category."));
            panel.Children.Add(SettingsRow("Reject Marketing",    "Deny marketing/advertising consent.",       "CoM_RejectMarketing",    SettingsService.Current.CoM_RejectMarketing,    v => SettingsService.Current.CoM_RejectMarketing    = v));
            panel.Children.Add(SettingsRow("Reject Analytics",    "Deny analytics/statistics consent.",        "CoM_RejectAnalytics",    SettingsService.Current.CoM_RejectAnalytics,    v => SettingsService.Current.CoM_RejectAnalytics    = v));
            panel.Children.Add(SettingsRow("Reject Preferences",  "Deny personalisation/preference consent.",  "CoM_RejectPreferences",  SettingsService.Current.CoM_RejectPreferences,  v => SettingsService.Current.CoM_RejectPreferences  = v));
            panel.Children.Add(SettingsRow("Reject All Others",   "Deny any unrecognised consent category.",   "CoM_RejectOthers",       SettingsService.Current.CoM_RejectOthers,       v => SettingsService.Current.CoM_RejectOthers       = v));
            panel.Children.Add(SettingsNote("These preferences are passed to the extension at startup. Changes take effect after restart."));
            return panelBorder;
        }

        // Generic installed extension — show install date + folder button
        panel.Children.Add(SettingsNote($"Installed: {ext.InstalledAt:yyyy-MM-dd}"));
        var folderBtn = new Button
        {
            Content = "📂 Open Extension Folder",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        string captFolder = ExtensionService.GetInstallFolder(ext.Id);
        folderBtn.Click += (_, _) =>
        {
            try { if (Directory.Exists(captFolder)) Process.Start("explorer.exe", captFolder); }
            catch { }
        };
        panel.Children.Add(folderBtn);

        return panelBorder;
    }

    // ── Settings panel helpers ────────────────────────────────────────────────

    private static TextBlock SettingsNote(string text) => new()
    {
        Text = text,
        Foreground   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        FontSize     = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin       = new Thickness(0, 0, 0, 6),
    };

    /// <summary>Creates a label + checkbox row wired to a bool setting via an Action setter.</summary>
    private static Grid SettingsRow(string label, string tooltip, string settingKey, bool currentValue, Action<bool> setter)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
        };
        Grid.SetColumn(lbl, 0);

        var chk = new CheckBox
        {
            IsChecked = currentValue,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
        };
        Grid.SetColumn(chk, 1);

        chk.Checked   += (_, _) => { setter(true);  SettingsService.Save(); };
        chk.Unchecked += (_, _) => { setter(false); SettingsService.Save(); };

        g.Children.Add(lbl);
        g.Children.Add(chk);
        return g;
    }

    private void SliderPaletteSampleRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TblPaletteSampleRateValue != null)
            TblPaletteSampleRateValue.Text = $"{e.NewValue:F1} s";
    }

    private void SliderScrollSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TblScrollSpeedValue != null)
            TblScrollSpeedValue.Text = $"{e.NewValue:F2}×";
    }

    private static SolidColorBrush ExtBorderBrush(bool enabled)
        => new(enabled ? Color.FromRgb(0x00, 0x55, 0x00) : Color.FromRgb(0x2A, 0x2A, 0x2A));

    private static void ApplyToggleStyle(Button btn, bool on)
    {
        btn.Background  = new SolidColorBrush(on ? Color.FromRgb(0x00, 0x33, 0x00) : Color.FromRgb(0x22, 0x22, 0x22));
        btn.Foreground  = new SolidColorBrush(on ? Color.FromRgb(0x00, 0xFF, 0x00) : Color.FromRgb(0x55, 0x55, 0x55));
        btn.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x00, 0x88, 0x00) : Color.FromRgb(0x33, 0x33, 0x33));
    }

    // ── All other handlers unchanged from original ────────────────────────────

    private async void BtnSyncCookies_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow mw) return;

        BtnSyncCookies.IsEnabled = false;
        TblCookieSyncStatus.Text = "Syncing...";

        bool useChrome = RboCookieChrome.IsChecked == true || RboCookieBoth.IsChecked == true;
        bool useEdge   = RboCookieEdge.IsChecked   == true || RboCookieBoth.IsChecked == true;
        string? domainFilter = null;

        if (RboCookieScopeCurrent.IsChecked == true)
        {
            string? currentUrl = mw.CurrentBrowser?.MainWebView?.Source?.Host;
            if (!string.IsNullOrEmpty(currentUrl))
                domainFilter = currentUrl;
        }

        try
        {
            int count = await mw.SyncCookiesFromBrowserAsync(useChrome, useEdge, domainFilter);
            TblCookieSyncStatus.Text = count > 0
                ? $"✓ {count} cookies synced. Reload open pages to apply."
                : "No cookies found for the selected source/scope.";
            LogService.Write("COOKIES", $"Sync completed: {count} cookies (chrome={useChrome}, edge={useEdge}, domain={domainFilter ?? "all"})");
        }
        catch (Exception ex)
        {
            TblCookieSyncStatus.Text = $"Sync failed: {ex.Message}";
            LogService.RecordCrash(ex, "CookieSyncWizard");
        }
        finally
        {
            BtnSyncCookies.IsEnabled = true;
        }
    }

    private void BtnMapProject_Click(object sender, RoutedEventArgs e)
    {
        string? target = ResolvePath("start_mapper.bat");
        if (target != null) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) });
        else MessageBox.Show("start_mapper.bat not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*", Title = "Import Credentials to Vault" };
        if (dlg.ShowDialog() == true) VaultService.ImportCsv(dlg.FileName);
    }

    private void BtnImportBookmarks_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "HTML Files (*.html)|*.html|All files (*.*)|*.*", Title = "Select Bookmark HTML File" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int count = BookmarkService.ImportHtml(dlg.FileName);
            MessageBox.Show(count > 0 ? $"{count} bookmarks imported." : "No bookmarks found.",
                "Import", MessageBoxButton.OK, count > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "Settings Bookmark Import"); MessageBox.Show("Import failed.\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    

    private void BtnNuclear_Click(object sender, RoutedEventArgs e)
    { HealthService.NuclearPurge(); MessageBox.Show("Memory Purge Executed.", "System Health", MessageBoxButton.OK, MessageBoxImage.Exclamation); }

    private void BtnOpenMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileInfo? newest = null;
            foreach (var dir in GetSearchPaths())
            {
                if (!Directory.Exists(dir)) continue;
                var f = new DirectoryInfo(dir).GetFiles("project_map_*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (f != null && (newest == null || f.LastWriteTime > newest.LastWriteTime)) newest = f;
            }
            if (newest != null) Process.Start("notepad.exe", newest.FullName);
            else MessageBox.Show("No project map files found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void BtnResetPdfPref_Click(object sender, RoutedEventArgs e)
    {
        Controls.BrowserView.ResetPdfPreference();
        MessageBox.Show("PDF open preference cleared.", "Horizon", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnBackupUpdate_Click(object sender, RoutedEventArgs e)
    {
        string? target = ResolvePath("Update_manager.bat");
        if (target != null) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) });
        else MessageBox.Show("Update_manager.bat not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        string? updateTxtPath = ResolvePath("update.txt");
        string? versionTxtPath = ResolvePath("version.txt");

        if (updateTxtPath == null) { MessageBox.Show("update.txt not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (versionTxtPath == null) { MessageBox.Show("version.txt not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

        BtnCheckForUpdates.IsEnabled = false;
        BtnCheckForUpdates.Content = "CHECKING...";

        var result = await Services.GithubUpdateService.CheckForUpdateAsync(updateTxtPath, versionTxtPath);

        BtnCheckForUpdates.IsEnabled = true;
        BtnCheckForUpdates.Content = "CHECK FOR UPDATES";

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            MessageBox.Show(result.ErrorMessage, "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!result.UpdateAvailable)
        {
            MessageBox.Show($"You are running the latest {result.CurrentChannel} version ({result.CurrentVersion}).",
                "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ask = MessageBox.Show(
            $"Update available: {result.LatestVersion} ({result.CurrentChannel})\nCurrent version: {result.CurrentVersion}\n\nDownload now?",
            "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (ask != MessageBoxResult.Yes) return;
        if (string.IsNullOrEmpty(result.AssetUrl) || string.IsNullOrEmpty(result.AssetName)) return;

        await DownloadUpdateAsync(result.AssetUrl, result.AssetName);
    }

    private async Task DownloadUpdateAsync(string assetUrl, string assetName)
    {
        BtnCheckForUpdates.IsEnabled = false;
        BtnCheckForUpdates.Content = "DOWNLOADING...";

        try
        {
            string path = await Services.GithubUpdateService.DownloadAssetAsync(assetUrl, assetName);
            SettingsService.Current.PendingUpdateInstallerPath = path;
            SettingsService.Save();
            Services.GithubUpdateService.NotifyUpdateReady(path);
            MessageBox.Show($"Downloaded to:\n{path}\n\nAn \"INSTALL UPDATE\" button is now available in the browser header.",
                "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnCheckForUpdates.IsEnabled = true;
            BtnCheckForUpdates.Content = "CHECK FOR UPDATES";
        }
    }

    private void BtnOpenExtensionsFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(ExtensionService.InstallRoot); Process.Start("explorer.exe", ExtensionService.InstallRoot); }
        catch (Exception ex) { MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void LoadDefaultGoogleAccountCombo()
    {
        CboDefaultGoogleAccount.Items.Clear();

        var accounts = SettingsService.Current.GoogleBrowserAccounts;

        if (accounts == null || !accounts.Any())
        {
            TblDefaultGoogleAccountHint.Visibility = Visibility.Visible;
            CboDefaultGoogleAccount.Visibility = Visibility.Collapsed;
            return;
        }

        TblDefaultGoogleAccountHint.Visibility = Visibility.Collapsed;
        CboDefaultGoogleAccount.Visibility = Visibility.Visible;

        var noneItem = new ComboBoxItem { Content = "(None)", Tag = "" };
        CboDefaultGoogleAccount.Items.Add(noneItem);

        string saved = SettingsService.Current.DefaultGoogleAccountEmail;
        ComboBoxItem? toSelect = noneItem;

        foreach (var acc in accounts)
        {
            string label = string.IsNullOrEmpty(acc.Name) ? acc.Email : $"{acc.Name} ({acc.Email})";
            var item = new ComboBoxItem { Content = label, Tag = acc.Email };
            CboDefaultGoogleAccount.Items.Add(item);
            if (acc.Email == saved) toSelect = item;
        }

        CboDefaultGoogleAccount.SelectedItem = toSelect;
    }

    private void SelectComboBoxByTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem ci && ci.Tag?.ToString() == tag) { combo.SelectedItem = ci; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void CmbNarrowWindowMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        SettingsService.Current.NarrowWindowMode = CmbNarrowWindowMode.SelectedIndex;
        SettingsService.Save();
    }

    private void TxtNarrowThreshold_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtNarrowThreshold.Text, out int val) && val >= 400)
        {
            SettingsService.Current.NarrowWindowThresholdPx = val;
            SettingsService.Save();
        }
        else
        {
            TxtNarrowThreshold.Text = SettingsService.Current.NarrowWindowThresholdPx.ToString();
        }
    }

    private void NumericOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}